import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Scroller from 'Components/Scroller/Scroller';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { scrollDirections } from 'Helpers/Props';
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

  render() {
    const {
      items,
      isFetching,
      currentBookId,
      onBookSelect,
      onModalClose
    } = this.props;

    const filter = this.state.filter.toLowerCase();

    const filtered = filter ?
      items.filter((book) => book.title.toLowerCase().includes(filter)) :
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
                            releaseDate={item.releaseDate}
                            editions={item.editions}
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
  currentBookId: PropTypes.number,
  onBookSelect: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default ChangeBookModalContent;
