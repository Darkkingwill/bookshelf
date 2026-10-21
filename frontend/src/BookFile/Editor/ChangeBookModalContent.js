import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Scroller from 'Components/Scroller/Scroller';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds, scrollDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ChangeBookRow from './ChangeBookRow';
import styles from './ChangeBookModalContent.css';

const columns = [
  {
    name: 'title',
    label: 'Book Title',
    isVisible: true
  },
  {
    name: 'releaseDate',
    label: 'Release Date',
    isVisible: true
  }
];

class ChangeBookModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      filter: ''
    };
  }

  //
  // Listeners

  onFilterChange = ({ value }) => {
    this.setState({ filter: value });
  };

  //
  // Render

  renderConfirm() {
    const {
      items,
      isSaving,
      hasSaveError,
      fileCount,
      currentBookId,
      pendingBook,
      onConfirm,
      onBack,
      onModalClose
    } = this.props;

    const currentBook = items.find((book) => book.id === currentBookId);

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('ChangeBook')}
        </ModalHeader>

        <ModalBody>
          <div>
            {translate('ChangeBookConfirmMessage', [fileCount, currentBook ? currentBook.title : '?', pendingBook.title])}
          </div>

          {
            hasSaveError ?
              <Alert
                className={styles.error}
                kind={kinds.DANGER}
              >
                {translate('ChangeBookFailed')}
              </Alert> :
              null
          }
        </ModalBody>

        <ModalFooter>
          <Button
            isDisabled={isSaving}
            onPress={onBack}
          >
            {translate('Back')}
          </Button>

          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isSaving}
            onPress={onConfirm}
          >
            {translate('ChangeBook')}
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }

  render() {
    const {
      items,
      isFetching,
      currentBookId,
      pendingBook,
      noEditionBook,
      hasResolveError,
      onBookSelect,
      onModalClose
    } = this.props;

    if (pendingBook) {
      return this.renderConfirm();
    }

    const filter = this.state.filter.toLowerCase();

    const filtered = filter ?
      items.filter((book) => {
        return book.title.toLowerCase().includes(filter) ||
          (book.seriesTitle || '').toLowerCase().includes(filter);
      }) :
      items;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('ChangeBook')}
        </ModalHeader>

        <ModalBody
          className={styles.modalBody}
          scrollDirection={scrollDirections.NONE}
        >
          <TextInput
            className={styles.filterInput}
            placeholder={translate('FilterBooksPlaceholder')}
            name="filter"
            value={this.state.filter}
            autoFocus={true}
            onChange={this.onFilterChange}
          />

          {
            noEditionBook ?
              <Alert
                className={styles.error}
                kind={kinds.WARNING}
              >
                {translate('ChangeBookNoEdition', [noEditionBook.title])}
              </Alert> :
              null
          }

          {
            hasResolveError ?
              <Alert
                className={styles.error}
                kind={kinds.DANGER}
              >
                {translate('ChangeBookFailed')}
              </Alert> :
              null
          }

          <Scroller
            className={styles.scroller}
            autoFocus={false}
          >
            {
              isFetching ?
                <LoadingIndicator /> :
                <Table columns={columns}>
                  <TableBody>
                    {
                      filtered.map((item) => {
                        return (
                          <ChangeBookRow
                            key={item.id}
                            id={item.id}
                            title={item.title}
                            seriesTitle={item.seriesTitle}
                            releaseDate={item.releaseDate}
                            isCurrent={item.id === currentBookId}
                            onBookSelect={onBookSelect}
                          />
                        );
                      })
                    }
                  </TableBody>
                </Table>
            }
          </Scroller>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            {translate('Cancel')}
          </Button>
        </ModalFooter>
      </ModalContent>
    );
  }
}

ChangeBookModalContent.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  isFetching: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  hasSaveError: PropTypes.bool.isRequired,
  fileCount: PropTypes.number.isRequired,
  currentBookId: PropTypes.number,
  pendingBook: PropTypes.object,
  noEditionBook: PropTypes.object,
  hasResolveError: PropTypes.bool.isRequired,
  onBookSelect: PropTypes.func.isRequired,
  onConfirm: PropTypes.func.isRequired,
  onBack: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default ChangeBookModalContent;
